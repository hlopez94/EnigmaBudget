import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { canActivateAuth } from './auth-guard';
import { LoginComponent } from './login/login.component';
import { ProfileComponent } from './profile/profile.component';
import { SignupComponent } from './signup/signup.component';
import { UnverifiedAccountComponent } from './unverified-account/unverified-account.component';
import { ValidateAccountComponent } from './validate-account/validate-account.component';

export const AUTH_ROUTES: Routes = [
  {
    path: 'login',
    component: LoginComponent,
  },
  {
    path: 'unverified-account',
    component: UnverifiedAccountComponent,
  },
  {
    path: 'profile',
    component: ProfileComponent,
    canActivate: [canActivateAuth],
  },
  {
    path: 'signup',
    component: SignupComponent,
  },
  {
    path: 'validate-mail',
    component: ValidateAccountComponent,
  },
];

@NgModule({
  imports: [RouterModule.forChild(AUTH_ROUTES)],
  exports: [RouterModule],
})
export class AuthRoutingModule {}
