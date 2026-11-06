# 02. Chuẩn Bị Hạ Tầng AWS

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-AWS-02` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Phạm vi áp dụng** | EC2, Security Group, Elastic IP, DNS, Docker trên máy chủ |

---

Các bước dưới đây làm trên AWS Console và trên máy chủ qua SSH. Thông số máy là mức khuyến nghị cho hệ thống này; tài liệu không ghi loại instance đang chạy thực tế, kiểm tra trên Console khi cần.

## 1. Tạo EC2

| Mục | Giá trị khuyến nghị | Lý do |
| :--- | :--- | :--- |
| AMI | Ubuntu Server 24.04 LTS (x86_64) | Image .NET, Node, PostgreSQL đều có bản amd64 |
| Loại máy | tối thiểu 4 GB RAM (ví dụ `t3.medium`) | `dotnet publish` và `vite build` chạy ngay trên máy lúc deploy. Máy 2 GB cần thêm swap (mục 5) |
| Ổ đĩa | EBS gp3, tối thiểu 30 GB | Image SDK .NET và Node lớn, cộng dữ liệu PostgreSQL và cache build |
| Key pair | tạo mới, giữ file `.pem` an toàn | Đăng nhập SSH |
| Region | gần người dùng (ví dụ `ap-southeast-1` Singapore) | Giảm độ trễ |

Nên bật **Termination protection** và để ổ EBS gốc **không** xóa khi terminate nếu muốn an toàn dữ liệu.

## 2. Security Group

| Chiều | Cổng | Nguồn | Mục đích |
| :--- | :---: | :--- | :--- |
| Inbound | 22/TCP | IP của người quản trị (`x.x.x.x/32`) | SSH |
| Inbound | 80/TCP | `0.0.0.0/0`, `::/0` | Let's Encrypt kiểm tra HTTP-01 và chuyển hướng sang HTTPS |
| Inbound | 443/TCP | `0.0.0.0/0`, `::/0` | HTTPS cho Admin, POS, API, SignalR |
| Outbound | tất cả | `0.0.0.0/0` | Kéo image Docker, gói npm/NuGet, gọi Let's Encrypt |

Không mở 5000, 5173, 5174, 5432, 6379, 5050. Bản production cũng không publish các cổng này, Security Group là lớp chặn thứ hai.

## 3. Elastic IP

1. EC2 → Elastic IPs → Allocate, rồi Associate vào instance.
2. Không có Elastic IP thì IP công khai đổi mỗi lần Stop/Start, DNS sẽ trỏ sai và Caddy không gia hạn được chứng chỉ.
3. Elastic IP gắn vào máy đang chạy vẫn tính phí IPv4 công khai theo giờ; giải phóng nếu bỏ hẳn hệ thống.

## 4. DNS

Tạo hai bản ghi `A` trỏ về Elastic IP, ví dụ với domain `khoadang.site`:

| Tên | Loại | Giá trị |
| :--- | :---: | :--- |
| `oisms` | `A` | `<Elastic IP>` |
| `pos.oisms` | `A` | `<Elastic IP>` |

Kiểm tra: `dig +short oisms.khoadang.site` phải ra Elastic IP trước khi chạy Caddy. Nếu nhà cung cấp DNS có proxy (ví dụ Cloudflare đám mây cam), tắt proxy hoặc cấu hình SSL "Full (strict)", nếu không Caddy khó xin chứng chỉ bằng HTTP-01.

Chưa có domain: dùng `sslip.io` theo Elastic IP, ví dụ `admin.203.0.113.10.sslip.io`, `pos.203.0.113.10.sslip.io` (đây là giá trị mẫu trong `.env.production.example`). Let's Encrypt vẫn cấp chứng chỉ được cho tên này.

## 5. Cài Docker Và Chuẩn Bị Máy

```bash
ssh -i oisms.pem ubuntu@<Elastic IP>
```

```bash
sudo apt-get update && sudo apt-get upgrade -y
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker ubuntu
```

Đăng xuất rồi SSH lại để nhóm `docker` có hiệu lực, sau đó kiểm tra:

```bash
docker version
docker compose version
```

`docker-compose.prod.yml` dùng thẻ `!reset` để xóa danh sách cổng của file gốc, cần **Docker Compose v2.24 trở lên** (plugin `docker compose`, không phải `docker-compose` v1).

Máy ít RAM thì thêm swap 4 GB để build không bị kill:

```bash
sudo fallocate -l 4G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

Đặt múi giờ hiển thị log (tùy chọn, không ảnh hưởng nghiệp vụ vì backend lưu UTC và báo cáo tự quy đổi `UTC+07:00`):

```bash
sudo timedatectl set-timezone Asia/Ho_Chi_Minh
```

## 6. Lấy Mã Nguồn

```bash
git clone <URL repo> ~/OISMS
cd ~/OISMS
git checkout main   # hoặc nhánh/tag cần deploy
```

Repo private thì dùng deploy key chỉ đọc hoặc Personal Access Token có hạn, không đặt khóa cá nhân lên máy chủ dùng chung.

Bước tiếp theo: [cấu hình môi trường production](./03-cau-hinh-moi-truong-production.md).
