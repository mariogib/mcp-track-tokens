import * as vscode from 'vscode';
import {
  formatStatusBarText,
  formatStatusBarTooltip,
} from './statusBarUi';
import type { TrackingState } from './types';

/**
 * Status bar: $(record) Track: Project Name with Tracking/Paused/Unallocated/Server Offline.
 */
export class StatusBarController implements vscode.Disposable {
  private readonly item: vscode.StatusBarItem;
  private state: TrackingState = 'Paused';
  private projectName = 'No project';
  private visible = true;

  constructor() {
    this.item = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
    this.item.command = 'mcpTrackTokens.showStatus';
    this.refresh();
  }

  setVisible(visible: boolean): void {
    this.visible = visible;
    if (visible) {
      this.item.show();
    } else {
      this.item.hide();
    }
  }

  update(state: TrackingState, projectName: string): void {
    this.state = state;
    this.projectName = projectName || 'No project';
    this.refresh();
  }

  private refresh(): void {
    this.item.text = formatStatusBarText({ state: this.state, projectName: this.projectName });
    this.item.tooltip = formatStatusBarTooltip(this.state, this.projectName);
    this.item.backgroundColor =
      this.state === 'Server Offline'
        ? new vscode.ThemeColor('statusBarItem.errorBackground')
        : this.state === 'Unallocated'
          ? new vscode.ThemeColor('statusBarItem.warningBackground')
          : undefined;
    if (this.visible) {
      this.item.show();
    }
  }

  dispose(): void {
    this.item.dispose();
  }
}

export {
  formatStatusBarText,
  formatStatusBarTooltip,
  resolveStatusBarState,
} from './statusBarUi';
