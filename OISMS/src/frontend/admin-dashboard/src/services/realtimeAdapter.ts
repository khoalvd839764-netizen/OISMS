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
let status: RealtimeStatus = "connected";
let latestEvent: RealtimeEvent | null = null;

function notifyStatus(next: RealtimeStatus) {
  status = next;
  statusListeners.forEach((listener) => listener(status));
}

export const realtimeAdapter = {
  // Thay phần thân adapter bằng SignalR HubConnection khi backend sẵn sàng.
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
  emitMock(event: RealtimeEvent) {
    latestEvent = event;
    eventListeners.forEach((listener) => listener(event));
  },
  getLatestEvent() {
    return latestEvent;
  },
  mockDisconnect() {
    notifyStatus("disconnected");
    window.setTimeout(() => notifyStatus("reconnecting"), 900);
    window.setTimeout(() => notifyStatus("connected"), 2600);
  },
};
