import http from 'node:http';
import net from 'node:net';

/** Real TCP interruption of SignalR, while REST remains available to commit missed transactions. */
export async function createNetworkProxy(targetUrl) {
  const target=new URL(targetUrl), sockets=new Set(); let hubBlocked=false;
  const server=http.createServer((req,res)=>{
    if(hubBlocked&&req.url.startsWith('/hubs/')) {res.destroy();return;}
    const upstream=http.request({hostname:target.hostname,port:target.port,path:req.url,method:req.method,headers:{...req.headers,host:target.host}},r=>{res.writeHead(r.statusCode,r.headers);r.pipe(res);});
    upstream.on('error',()=>res.destroy());req.pipe(upstream);
  });
  server.on('upgrade',(req,client,head)=>{
    if(hubBlocked){client.destroy();return;}
    sockets.add(client);client.on('close',()=>sockets.delete(client));
    const upstream=net.connect(Number(target.port),target.hostname,()=>{
      upstream.write(`${req.method} ${req.url} HTTP/${req.httpVersion}\r\n`);
      for(let i=0;i<req.rawHeaders.length;i+=2)upstream.write(`${req.rawHeaders[i]}: ${req.rawHeaders[i+1]}\r\n`);
      upstream.write('\r\n');if(head.length)upstream.write(head);client.pipe(upstream);upstream.pipe(client);
    });
    sockets.add(upstream);upstream.on('close',()=>sockets.delete(upstream));upstream.on('error',()=>client.destroy());client.on('error',()=>upstream.destroy());
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  return {url:`http://127.0.0.1:${server.address().port}`,cut(){hubBlocked=true;for(const socket of sockets)socket.destroy();server.closeAllConnections();},restore(){hubBlocked=false;},async close(){for(const socket of sockets)socket.destroy();server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}};
}
