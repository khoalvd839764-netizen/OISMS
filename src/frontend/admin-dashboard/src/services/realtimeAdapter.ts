import {
  HubConnectionBuilder,
  HubConnection,
  LogLevel,
  HubConnectionState,
} from "@microsoft/signalr";

export type RealtimeStatus = "connected" | "reconnecting" | "disconnected";

export type RealtimeEvent =
  | {
      type: "order.created";
      id: string;
      title: string;
      message: string;
      orderCode: string;
      channel: string;
      href: string;
      createdAt: string;
    }
  | {
      type: "reservation.expired";
      id: string;
      title: string;
      message: string;
      orderCode: string;
      reason: string;
      href: string;
      createdAt: string;
    }
  | {
      type: "inventory.low";
      id: string;
      title: string;
      message: string;
      sku: string;
      warehouse: string;
      href: string;
      createdAt: string;
    };

type EventListener = (event: RealtimeEvent) => void;
type StatusListener = (status: RealtimeStatus) => void;

const eventListeners = new Set<EventListener>();
const statusListeners = new Set<StatusListener>();
let status: RealtimeStatus = "disconnected";
let latestEvent: RealtimeEvent | null = null;
let hubConnection: HubConnection | null = null;
let currentBranchId: string | null = null;
const joinedBranchIds = new Set<string>();
let currentConnectionToken: string | null = null;
let connectionGeneration = 0;

function notifyStatus(next: RealtimeStatus) {
  status = next;
  statusListeners.forEach((listener) => listener(status));
}

function getApiBaseUrl(): string {
  const envUrl = import.meta.env.VITE_API_BASE_URL || "http://localhost:5000";
  return envUrl.replace(/\/$/, "");
}

function initSignalRConnection(): HubConnection {
  const hubUrl = `${getApiBaseUrl()}/hubs/notifications`;

  const connection = new HubConnectionBuilder()
    .withUrl(hubUrl, {
      accessTokenFactory: () => {
        return localStorage.getItem("oism_access_token") || "";
      },
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build();

  // 1. Sự kiện đơn hàng mới toàn tenant
  connection.on("OrderCreated", (payload: any) => {
    if (connection !== hubConnection) return;
    const orderNumber = payload.orderNumber || payload.OrderNumber || "Mới";
    const orderId = payload.orderId || payload.OrderId || "";
    const channel = payload.channel || payload.Channel || "POS";
    const amount = Number(payload.totalAmount ?? payload.TotalAmount ?? 0);
    const customer = payload.customerName || payload.CustomerName;

    const event: RealtimeEvent = {
      type: "order.created",
      id: `ord-${orderId || Date.now()}`,
      title: `Đơn hàng mới #${orderNumber}`,
      message: `${customer ? customer + " · " : ""}${channel} · ${amount.toLocaleString("vi-VN")} đ`,
      orderCode: orderNumber,
      channel,
      href: `/orders?orderId=${orderId}`,
      createdAt: payload.createdAt || payload.CreatedAt || new Date().toISOString(),
    };

    latestEvent = event;
    eventListeners.forEach((listener) => listener(event));
    window.dispatchEvent(new CustomEvent("oism:order-created", { detail: payload }));
  });

  // 2. Sự kiện biến động tồn kho chi nhánh
  connection.on("StockLevelChanged", (payload: any) => {
    if (connection !== hubConnection) return;
    window.dispatchEvent(new CustomEvent("oism:stock-changed", { detail: payload }));
  });

  // 3. Cảnh báo tồn kho dưới ngưỡng an toàn
  connection.on("LowStockWarning", (payload: any) => {
    if (connection !== hubConnection) return;
    const skuCode = payload.skuCode || payload.SkuCode || "SKU";
    const available = Number(payload.currentAvailable ?? payload.CurrentAvailable ?? 0);
    const threshold = Number(payload.threshold ?? payload.Threshold ?? 0);
    const skuId = payload.skuId || payload.SkuId || "";

    const event: RealtimeEvent = {
      type: "inventory.low",
      id: `low-${skuId}-${Date.now()}`,
      title: `Cảnh báo tồn kho: ${skuCode}`,
      message: `Tồn khả dụng còn ${available}, dưới ngưỡng ${threshold}`,
      sku: skuCode,
      warehouse: payload.branchName || payload.BranchName || "",
      href: `/reports?tab=alerts`,
      createdAt: payload.alertedAt || payload.AlertedAt || new Date().toISOString(),
    };

    latestEvent = event;
    eventListeners.forEach((listener) => listener(event));
    window.dispatchEvent(new CustomEvent("oism:low-stock", { detail: payload }));
  });

  // 4. Sự kiện hết hạn giữ chỗ kho (BF-016)
  connection.on("ReservationExpired", (payload: any) => {
    if (connection !== hubConnection) return;
    const orderCode = payload.orderNumber || payload.OrderNumber || payload.orderCode || "";
    const event: RealtimeEvent = {
      type: "reservation.expired",
      id: `res-${payload.orderId || Date.now()}`,
      title: `Hết hạn giữ chỗ #${orderCode}`,
      message: payload.reason || "Đơn hàng đã quá hạn giữ chỗ tồn kho và tự động hủy.",
      orderCode,
      reason: payload.reason || "Timeout",
      href: `/orders?orderId=${payload.orderId || ""}`,
      createdAt: payload.createdAt || new Date().toISOString(),
    };

    latestEvent = event;
    eventListeners.forEach((listener) => listener(event));
    window.dispatchEvent(new CustomEvent("oism:reservation-expired", { detail: payload }));
  });

  // Trạng thái vòng đời kết nối (BF-002: Chặn callback từ kết nối cũ đã bị thay thế)
  connection.onreconnecting(() => {
    if (connection !== hubConnection) return;
    notifyStatus("reconnecting");
  });

  connection.onreconnected(() => {
    if (connection !== hubConnection) return;
    notifyStatus("connected");
    const branchesToRejoin = new Set<string>(joinedBranchIds);
    if (currentBranchId) branchesToRejoin.add(currentBranchId);
    branchesToRejoin.forEach((bId) => {
      connection.invoke("JoinBranchGroup", bId).catch((err) => {
        console.warn("Lỗi khi tham gia lại nhóm chi nhánh sau reconnect:", err);
      });
    });
    window.dispatchEvent(new CustomEvent("oism:realtime-reconnected"));
  });

  connection.onclose(() => {
    if (connection !== hubConnection) return;
    notifyStatus("disconnected");
  });

  return connection;
}

export const realtimeAdapter = {
  subscribe(listener: EventListener) {
    eventListeners.add(listener);
    return () => {
      eventListeners.delete(listener);
    };
  },

  subscribeStatus(listener: StatusListener) {
    statusListeners.add(listener);
    listener(status);
    return () => {
      statusListeners.delete(listener);
    };
  },

  async start(): Promise<void> {
    const token = localStorage.getItem("oism_access_token");
    if (!token) {
      notifyStatus("disconnected");
      return;
    }

    // [BF-002] Nếu token phiên hiện tại khác với token của kết nối đang có,
    // ngắt kết nối cũ và tạo kết nối mới độc lập cho phiên mới.
    if (hubConnection && currentConnectionToken && currentConnectionToken !== token) {
      const oldConn = hubConnection;
      hubConnection = null;
      currentConnectionToken = null;
      connectionGeneration++;
      try {
        await oldConn.stop();
      } catch {}
    }

    if (hubConnection && hubConnection.state === HubConnectionState.Connected && currentConnectionToken === token) {
      notifyStatus("connected");
      return;
    }

    const startGen = ++connectionGeneration;

    try {
      if (!hubConnection || currentConnectionToken !== token) {
        hubConnection = initSignalRConnection();
        currentConnectionToken = token;
      }

      const activeConn = hubConnection;
      if (activeConn.state === HubConnectionState.Disconnected) {
        await activeConn.start();
        if (startGen !== connectionGeneration || activeConn !== hubConnection) {
          try {
            await activeConn.stop();
          } catch {}
          return;
        }
        notifyStatus("connected");

        const branchesToJoin = new Set<string>(joinedBranchIds);
        if (currentBranchId) branchesToJoin.add(currentBranchId);
        for (const bId of branchesToJoin) {
          await activeConn.invoke("JoinBranchGroup", bId).catch(console.warn);
        }
      }
    } catch (error) {
      if (startGen === connectionGeneration) {
        console.warn("SignalR Hub connection failed (sẽ tự động thử lại):", error);
        notifyStatus("disconnected");
      }
    }
  },

  async stop(): Promise<void> {
    connectionGeneration++;
    joinedBranchIds.clear();
    if (hubConnection) {
      const conn = hubConnection;
      hubConnection = null;
      currentConnectionToken = null;
      currentBranchId = null;
      latestEvent = null;
      try {
        await conn.stop();
      } catch (err) {
        console.error("Lỗi khi đóng kết nối SignalR:", err);
      } finally {
        notifyStatus("disconnected");
      }
    } else {
      currentConnectionToken = null;
      currentBranchId = null;
      latestEvent = null;
      notifyStatus("disconnected");
    }
  },

  async joinBranch(branchId: string): Promise<void> {
    if (!branchId) return;
    currentBranchId = branchId;
    joinedBranchIds.add(branchId);
    if (hubConnection && hubConnection.state === HubConnectionState.Connected && branchId) {
      try {
        await hubConnection.invoke("JoinBranchGroup", branchId);
      } catch (err) {
        console.warn(`Lỗi khi tham gia nhóm chi nhánh ${branchId}:`, err);
      }
    }
  },

  async leaveBranch(branchId: string): Promise<void> {
    if (!branchId) return;
    joinedBranchIds.delete(branchId);
    if (currentBranchId === branchId) {
      currentBranchId = null;
    }
    if (hubConnection && hubConnection.state === HubConnectionState.Connected && branchId) {
      try {
        await hubConnection.invoke("LeaveBranchGroup", branchId);
      } catch (err) {
        console.warn(`Lỗi khi rời nhóm chi nhánh ${branchId}:`, err);
      }
    }
  },

  emitMock(event: RealtimeEvent) {
    latestEvent = event;
    eventListeners.forEach((listener) => listener(event));
  },

  getLatestEvent() {
    return latestEvent;
  },

  getStatus(): RealtimeStatus {
    return status;
  },

  mockDisconnect() {
    notifyStatus("disconnected");
    window.setTimeout(() => notifyStatus("reconnecting"), 900);
    window.setTimeout(() => notifyStatus("connected"), 2600);
  },
};
