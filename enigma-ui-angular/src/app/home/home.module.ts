import { HomeComponent } from './home.component';
import { GenerarCuentaDepositoModule } from './../core/dialogs/generar-cuenta-deposito/generar-cuenta-deposito.module';
import { MatDialogModule } from '@angular/material/dialog';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

import { HomeRoutingModule } from './home-routing.module';
import { MatMenuModule } from '@angular/material/menu';


@NgModule({
  declarations: [HomeComponent],
  imports: [
    CommonModule,
    HomeRoutingModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatListModule,
    MatDialogModule,
    GenerarCuentaDepositoModule,
    MatMenuModule
  ]
})
export class HomeModule { }
