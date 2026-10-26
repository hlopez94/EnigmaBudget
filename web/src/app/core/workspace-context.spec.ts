import '@angular/compiler';
import { describe, expect, it, vi } from 'vitest';
import { ApiService } from './api.service';
import { WorkspaceContext } from './workspace-context';
import { AuthEpoch } from './auth-epoch';

const response = (body: unknown): Response =>
  ({ ok: true, status: 200, json: async () => body }) as Response;

describe('workspace context boundaries', () => {
  it('Auth_epoch_advances_on_identity_and_workspace_changes', () => {
    const epoch = new AuthEpoch();
    const signedIn = epoch.snapshot();
    epoch.advance('user-a', null);
    const user = epoch.snapshot();
    epoch.advance('user-a', 'space-a');
    const workspace = epoch.snapshot();
    epoch.advance('user-b', null);
    const changedUser = epoch.snapshot();
    epoch.advance(null, null);
    const loggedOut = epoch.snapshot();
    expect([user.epoch, workspace.epoch, changedUser.epoch, loggedOut.epoch]).toEqual([1, 2, 3, 4]);
    expect(epoch.isCurrent(signedIn)).toBe(false);
    expect(epoch.isCurrent(workspace)).toBe(false);
    expect(epoch.isCurrent(loggedOut)).toBe(true);
  });
  it('captures the selected workspace on financial dispatch and leaves management unscoped', async () => {
    const calls: Array<{ url: string; init?: RequestInit }> = [];
    const api = new ApiService((async (input: RequestInfo | URL, init?: RequestInit) => {
      calls.push({ url: String(input), init });
      return String(input) === '/api/auth/session' ? response({ csrfToken: 'csrf' }) : response([]);
    }) as typeof fetch);

    await api.session();
    api.selectWorkspace('household-a');
    await api.accounts();
    api.selectWorkspace('household-b');
    await api.workspaces();
    await api.workspace('household-b');
    await api.loadOnboarding();
    await api.createWorkspace({ id: 'new-household', kind: 'Household', name: 'Casa' });
    await api.members('household-a');
    await api.invitations('household-a');
    await api.personalSharing();
    await api.previewInvitation('pending-token');

    expect(new Headers(calls[1].init?.headers).get('X-Workspace-Id')).toBe('household-a');
    expect(calls.filter(call => !call.url.startsWith('/api/accounts')).every(call => !new Headers(call.init?.headers).has('X-Workspace-Id'))).toBe(true);
  });

  it('sends JSON and CSRF headers with the workspace captured for a financial mutation', async () => {
    const calls: Array<{ url: string; init?: RequestInit }> = [];
    const api = new ApiService((async (input: RequestInfo | URL, init?: RequestInit) => {
      calls.push({ url: String(input), init });
      return String(input) === '/api/auth/session'
        ? response({ csrfToken: 'csrf-token' })
        : response({});
    }) as typeof fetch);

    await api.session();
    api.selectWorkspace('household-a');
    await api.createAccount({ name: 'Cuenta' });

    const headers = new Headers(calls[1].init?.headers);
    expect(headers.get('Content-Type')).toBe('application/json');
    expect(headers.get('X-CSRF-TOKEN')).toBe('csrf-token');
    expect(headers.get('X-Workspace-Id')).toBe('household-a');
  });

  it('does not publish a delayed overview after the selected workspace changes', async () => {
    const context = new WorkspaceContext();
    const oldResponse = deferred<{ label: string }>();
    context.select('old-space');
    const loadingOld = context.load(async () => oldResponse.promise);
    context.select('new-space');
    const loadingNew = context.load(async () => Promise.resolve({ label: 'nuevo' }));
    await loadingNew;
    oldResponse.resolve({ label: 'viejo' });
    await loadingOld;

    expect(context.value).toEqual({ label: 'nuevo' });
    expect(context.workspaceId).toBe('new-space');
  });

  it('asks for an explicit choice when a saved workspace is no longer accessible', () => {
    const context = new WorkspaceContext();
    sessionStorage.setItem('enigma-budget:workspace:user-1', 'revoked-space');

    const selected = context.restore('user-1', ['personal-space'], 'personal-space');

    expect(selected).toBe('');
    expect(context.workspaceId).toBe('');
  });
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => (resolve = done));
  return { promise, resolve };
}
