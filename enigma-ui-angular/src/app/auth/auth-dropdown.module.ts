import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

import { AuthGuard } from './auth-guard';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { AuthDropdownComponent } from './auth-dropdown/auth-dropdown.component';
import { AuthService } from './auth.service';
import { AuthDirectivesModule } from './auth-directives.module';
import { RouterModule } from '@angular/router';

@NgModule({
  declarations: [
    AuthDropdownComponent,
  ],
  imports: [
    CommonModule,
    MatIconModule,
    RouterModule,
    MatButtonModule,
    MatMenuModule,
    AuthDirectivesModule
  ],
  providers: [
    AuthService,
    AuthGuard
  ],
  exports: [AuthDropdownComponent],
})
export class AuthDropdownModule {}
