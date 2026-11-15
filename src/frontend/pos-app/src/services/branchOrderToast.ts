import type { BranchOrderCreated } from './realtimeService';

export const POS_SOUND_STORAGE_KEY = 'oisms_pos_v2_order_sound';

export function readOrderSoundEnabled(): boolean {
  try {
    return localStorage.getItem(POS_SOUND_STORAGE_KEY) !== 'off';
  } catch {
    return true;
  }
}

export function writeOrderSoundEnabled(enabled: boolean): void {
  try {
    localStorage.setItem(POS_SOUND_STORAGE_KEY, enabled ? 'on' : 'off');
  } catch {
    // ignore storage errors
  }
}

/** Deduplicate branch order toasts by orderId within the active POS session. */
export function appendUniqueBranchOrderToast(
  current: BranchOrderCreated[],
  seenOrderIds: Set<string>,
  incoming: BranchOrderCreated,
  maxVisible = 3,
): { next: BranchOrderCreated[]; added: boolean } {
  if (!incoming.orderId || seenOrderIds.has(incoming.orderId)) {
    return { next: current, added: false };
  }
  seenOrderIds.add(incoming.orderId);
  return {
    next: [incoming, ...current].slice(0, maxVisible),
    added: true,
  };
}

export function playPosOrderSound(): void {
  const AudioContextClass =
    window.AudioContext ||
    (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
  if (!AudioContextClass) return;
  try {
    const context = new AudioContextClass();
    const oscillator = context.createOscillator();
    const gain = context.createGain();
    oscillator.frequency.value = 780;
    gain.gain.setValueAtTime(0.08, context.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.001, context.currentTime + 0.2);
    oscillator.connect(gain);
    gain.connect(context.destination);
    oscillator.start();
    oscillator.stop(context.currentTime + 0.2);
  } catch {
    // Autoplay policy before first user gesture is expected and ignored.
  }
}
