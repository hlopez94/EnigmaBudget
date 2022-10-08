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
import {
  MatFormFieldModule,
  MAT_FORM_FIELD_DEFAULT_OPTIONS,
} from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ReactiveFormsModule } from '@angular/forms';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';
import { AuthDropdownComponent } from './auth-dropdown/auth-dropdown.component';
import { IfLoggedInDirective } from './directives/if-logged-in.directive';
import { IfNotLoggedInDirective } from './directives/if-not-logged-in.directive';
import { UnverifiedAccountComponent } from './unverified-account/unverified-account.component';
import { ValidateAccountComponent } from './validate-account/validate-account.component';

@NgModule({
  declarations: [
    LoginComponent,
    SignupComponent,
    ProfileComponent,
    AuthDropdownComponent,
    IfLoggedInDirective,
    IfNotLoggedInDirective,
    UnverifiedAccountComponent,
    ValidateAccountComponent
  ],
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
    MatDividerModule,
    MatSelectModule,
    MatAutocompleteModule,
  ],
  providers: [
    AuthGuard,
    {
      provide: MAT_FORM_FIELD_DEFAULT_OPTIONS,
      useValue: { appearance: 'outline' },
    },
  ],
  exports: [AuthDropdownComponent, IfLoggedInDirective, IfNotLoggedInDirective],
})
export class AuthModule {}
