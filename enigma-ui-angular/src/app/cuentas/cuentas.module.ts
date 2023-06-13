import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

import { CuentasRoutingModule } from './cuentas-routing.module';
import { CuentasComponent } from './cuentas.component';


@NgModule({
  declarations: [
    CuentasComponent
  ],
  imports: [
    CommonModule,
    CuentasRoutingModule,
  ]
})
export class CuentasModule { }
