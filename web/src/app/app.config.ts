import { ApplicationConfig } from '@angular/core';
import { provideOptimus } from '@openng/optimus-ui/config';
import Aura from '@openng/optimus-ui-themes/aura';
import { Capacitor } from '@capacitor/core';
import { API_FETCHER, Fetcher } from './core/api.service';
import { AuthEpoch } from './core/auth-epoch';
import { NATIVE_SNAPSHOT_READER, NativeApiFetcher } from './native/native-api-fetcher';

const webFetcher: Fetcher = (...args) => fetch(...args);
export const appConfig: ApplicationConfig = { providers: [
  provideOptimus({ theme: { preset: Aura } }),
  { provide: NATIVE_SNAPSHOT_READER, useFactory: (epoch: AuthEpoch) => () => epoch.snapshot(), deps: [AuthEpoch] },
  { provide: API_FETCHER, useFactory: (native: NativeApiFetcher) => Capacitor.getPlatform() === 'android' ? native.fetcher : webFetcher, deps: [NativeApiFetcher] },
] };
