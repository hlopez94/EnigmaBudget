import {
  BrowserAnimationsModule,
  provideAnimations,
} from '@angular/platform-browser/animations';
import { enableProdMode } from '@angular/core';
import { environment } from './environments/environment';
import { environmentLoader as environmentLoaderPromise } from './environments/environmentLoader';
import { BrowserModule, bootstrapApplication } from '@angular/platform-browser';
import { AppComponent } from './app/app.component';
import { AuthInterceptor } from './app/auth/auth-interceptor';
import {
  HTTP_INTERCEPTORS,
  provideHttpClient,
  withInterceptorsFromDi,
} from '@angular/common/http';
import { AppStateService } from './app/core/services/app-state.service';
import { AuthService } from './app/auth/auth.service';
import {
  PreloadAllModules,
  provideRouter,
  withPreloading,
} from '@angular/router';
import { APP_ROUTES } from './app/app-routing.module';
import { CommonModule } from '@angular/common';

environmentLoaderPromise.then((env) => {
  if (env.production) {
    enableProdMode();
  }
  environment.settings = env.settings;
});

bootstrapApplication(AppComponent, {
  providers: [
    CommonModule,
    BrowserModule,
    BrowserAnimationsModule,
    provideHttpClient(withInterceptorsFromDi()),
    provideAnimations(),
    provideRouter(APP_ROUTES, withPreloading(PreloadAllModules)),
    AuthService,
    AppStateService,
    {
      provide: HTTP_INTERCEPTORS,
      useClass: AuthInterceptor,
      multi: true,
    },
  ],
}).catch((err) => console.error(err));
