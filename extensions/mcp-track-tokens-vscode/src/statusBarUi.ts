import type { StatusBarModel, TrackingState } from './types';

/**
 * Pure status-bar presentation helpers.
 */
export function formatStatusBarText(model: StatusBarModel): string {
  return `$(record) Track: ${model.projectName}`;
}

export function formatStatusBarTooltip(state: TrackingState, projectName: string): string {
  return `MCP Track Tokens — ${state}\nProject: ${projectName}`;
}

export function resolveStatusBarState(input: {
  tracking: boolean;
  paused: boolean;
  hasProject: boolean;
  serverOnline: boolean;
}): TrackingState {
  if (!input.serverOnline) {
    return 'Server Offline';
  }
  if (input.paused || !input.tracking) {
    return 'Paused';
  }
  if (!input.hasProject) {
    return 'Unallocated';
  }
  return 'Tracking';
}
