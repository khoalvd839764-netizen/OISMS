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
let isStarting = false;

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
    window.dispatchEvent(new CustomEvent("oism:stock-changed", { detail: payload }));
  });

  // 3. Cảnh báo tồn kho dưới ngưỡng an toàn
  connection.on("LowStockWarning", (payload: any) => {
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

  // Trạng thái vòng đời kết nối
  connection.onreconnecting(() => {
    notifyStatus("reconnecting");
  });

  connection.onreconnected(() => {
    notifyStatus("connected");
    if (currentBranchId) {
      connection.invoke("JoinBranchGroup", currentBranchId).catch((err) => {
        console.warn("Lỗi khi tham gia lại nhóm chi nhánh sau reconnect:", err);
      });
    }
  });

  connection.onclose(() => {
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

    if (hubConnection && hubConnection.state === HubConnectionState.Connected) {
      notifyStatus("connected");
      return;
    }

    if (isStarting) return;
    isStarting = true;

    try {
      if (!hubConnection) {
        hubConnection = initSignalRConnection();
      }

      if (hubConnection.state === HubConnectionState.Disconnected) {
        await hubConnection.start();
        notifyStatus("connected");

        if (currentBranchId) {
          await hubConnection.invoke("JoinBranchGroup", currentBranchId).catch(console.warn);
        }
      }
    } catch (error) {
      console.warn("SignalR Hub connection failed (sẽ tự động thử lại):", error);
      notifyStatus("disconnected");
    } finally {
      isStarting = false;
    }
  },

  async stop(): Promise<void> {
    if (hubConnection) {
      try {
        await hubConnection.stop();
      } catch (err) {
        console.error("Lỗi khi đóng kết nối SignalR:", err);
      } finally {
        notifyStatus("disconnected");
      }
    }
  },

  async joinBranch(branchId: string): Promise<void> {
    currentBranchId = branchId;
    if (hubConnection && hubConnection.state === HubConnectionState.Connected && branchId) {
      try {
        await hubConnection.invoke("JoinBranchGroup", branchId);
      } catch (err) {
        console.warn(`Lỗi khi tham gia nhóm chi nhánh ${branchId}:`, err);
      }
    }
  },

  async leaveBranch(branchId: string): Promise<void> {
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
