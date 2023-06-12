import { GenerarCuentaDepositoModule } from './../core/dialogs/generar-cuenta-deposito/generar-cuenta-deposito.module';
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
    GenerarCuentaDepositoModule
  ]
})
export class CuentasModule { }
