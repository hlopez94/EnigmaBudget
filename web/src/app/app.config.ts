import { ApplicationConfig } from '@angular/core';
import { provideOptimus } from '@openng/optimus-ui/config';
import Aura from '@openng/optimus-ui-themes/aura';
export const appConfig: ApplicationConfig = { providers: [provideOptimus({ theme: { preset: Aura } })] };
