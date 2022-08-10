import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

import { AuthRoutingModule } from './auth-routing.module';
import { LoginComponent } from './login/login.component';
import { SignupComponent } from './signup/signup.component';
import { ProfileComponent } from './profile/profile.component';
import { AuthGuard } from './auth-guard';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ReactiveFormsModule } from '@angular/forms';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { MatMenuModule } from '@angular/material/menu';
import { AuthDropdownComponent } from './auth-dropdown/auth-dropdown.component';
import { IsLoggedInDirective } from './directives/is-logged-in.directive';
import { IsNotLoggedInDirective } from './directives/is-not-logged-in.directive';

@NgModule({
  declarations: [LoginComponent, SignupComponent, ProfileComponent, AuthDropdownComponent, IsLoggedInDirective, IsNotLoggedInDirective],
  imports: [
    CommonModule,
    AuthRoutingModule,
    MatCardModule,
    MatIconModule,
    MatButtonModule,
    ReactiveFormsModule,
    MatInputModule,
    MatFormFieldModule,
    MatSnackBarModule,
    MatMenuModule,
  ],
  providers: [AuthGuard],
  exports: [AuthDropdownComponent,IsLoggedInDirective, IsNotLoggedInDirective],
})
export class AuthModule {}
