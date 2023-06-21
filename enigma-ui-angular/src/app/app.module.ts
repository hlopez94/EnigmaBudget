import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { HttpClientModule, HTTP_INTERCEPTORS } from '@angular/common/http';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';

import { AppRoutingModule } from './app-routing.module';

import { AppComponent } from './app.component';
import { NavbarComponent } from './shell/navbar/navbar.component';
import { FooterComponent } from './shell/footer/footer.component';
import { SidebarComponent } from './shell/sidebar/sidebar.component';

//Modulos Material
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { AuthInterceptor } from './auth/auth-interceptor';
import { MatCardModule } from '@angular/material/card';
import { MatMenuModule } from '@angular/material/menu';
import { WelcomePageComponent } from './shell/welcome-page/welcome-page.component';
import { AuthDropdownModule } from './auth/auth-dropdown.module';
import { AuthService } from './auth/auth.service';
import { AppStateService } from './core/services/app-state.service';
import { IfLoggedInDirective } from './auth/directives/if-logged-in.directive';
import { IfNotLoggedInDirective } from './auth/directives/if-not-logged-in.directive';

@NgModule({
  declarations: [
    AppComponent,
    FooterComponent,
    SidebarComponent,
  ],
  imports: [
    NavbarComponent,
    WelcomePageComponent,
    BrowserModule,
    BrowserAnimationsModule,
    HttpClientModule,
    AppRoutingModule,
    AuthDropdownModule,

    IfLoggedInDirective,
    IfNotLoggedInDirective,

    //MaterialModules
    MatToolbarModule,
    MatIconModule,
    MatButtonModule,
    MatSidenavModule,
    MatCardModule,
    MatMenuModule,
  ],
  providers: [
    AuthService,
    AppStateService,
    {
      provide: HTTP_INTERCEPTORS,
      useClass: AuthInterceptor,
      multi: true,
    },
  ],
  bootstrap: [AppComponent],
})
export class AppModule {}
