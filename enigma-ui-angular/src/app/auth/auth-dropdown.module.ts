import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

import { AuthGuard } from './auth-guard';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS} from '@angular/material/form-field';
import { MatMenuModule } from '@angular/material/menu';
import { AuthDropdownComponent } from './auth-dropdown/auth-dropdown.component';

@NgModule({
  declarations: [
    AuthDropdownComponent,
  ],
  imports: [
    CommonModule,
    MatIconModule,
    MatButtonModule,
    MatMenuModule,
  ],
  providers: [
    AuthGuard,
    {
      provide: MAT_FORM_FIELD_DEFAULT_OPTIONS,
      useValue: { appearance: 'outline' },
    },
  ],
  exports: [AuthDropdownComponent],
})
export class AuthDropdownModule {}
