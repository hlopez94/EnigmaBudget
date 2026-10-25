import { Injectable } from '@angular/core';

export interface AuthEpochSnapshot {
  epoch: number;
  userId: string | null;
  workspaceId: string | null;
}

@Injectable({ providedIn: 'root' })
export class AuthEpoch {
  private current: AuthEpochSnapshot = { epoch: 0, userId: null, workspaceId: null };

  snapshot(): AuthEpochSnapshot { return { ...this.current }; }

  advance(userId: string | null, workspaceId: string | null): void {
    this.current = { epoch: this.current.epoch + 1, userId, workspaceId };
  }

  isCurrent(snapshot: AuthEpochSnapshot): boolean {
    return snapshot.epoch === this.current.epoch && snapshot.userId === this.current.userId && snapshot.workspaceId === this.current.workspaceId;
  }
}
