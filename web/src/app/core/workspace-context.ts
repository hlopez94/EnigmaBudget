import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class WorkspaceContext {
  private preferenceKey: string | null = null;
  workspaceId: string | null = null;
  value: unknown = null;
  generation = 0;
  saving = false;

  restore(userId: string, availableIds: string[], fallbackId: string, commit = true): string {
    this.preferenceKey = `enigma-budget:workspace:${userId}`;
    let selected: string | null = null;
    let preferenceRead = false;
    try {
      selected = sessionStorage.getItem(this.preferenceKey);
      preferenceRead = true;
    } catch {
      // Storage may be unavailable in a restricted browser context.
    }
    const next = selected ? (availableIds.includes(selected) ? selected : '') : fallbackId;
    if (!preferenceRead && selected === null) this.preferenceKey = null;
    if (commit) this.select(next);
    return next;
  }

  select(workspaceId: string | null): void {
    if (workspaceId === this.workspaceId) return;
    this.workspaceId = workspaceId;
    this.value = null;
    this.generation++;
    if (this.preferenceKey && workspaceId) {
      try {
        sessionStorage.setItem(this.preferenceKey, workspaceId);
      } catch {
        // Selection still works for this page when storage is unavailable.
      }
    } else if (this.preferenceKey) {
      try { sessionStorage.removeItem(this.preferenceKey); } catch { /* No persisted value to clear. */ }
    }
  }

  async load<T>(loader: () => Promise<T>): Promise<void> {
    const generation = this.generation;
    const workspaceId = this.workspaceId;
    const value = await loader();
    if (generation === this.generation && workspaceId === this.workspaceId) this.value = value;
  }

  clear(): void {
    if (this.preferenceKey) {
      try {
        sessionStorage.removeItem(this.preferenceKey);
      } catch {
        // Clearing in-memory context remains mandatory.
      }
    }
    this.preferenceKey = null;
    this.workspaceId = null;
    this.value = null;
    this.saving = false;
    this.generation++;
  }
}
