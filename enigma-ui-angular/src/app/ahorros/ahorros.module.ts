import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

import { AhorrosRoutingModule } from './ahorros-routing.module';
import { AhorrosComponent } from './ahorros.component';


@NgModule({
  declarations: [
    AhorrosComponent
  ],
  imports: [
    CommonModule,
    AhorrosRoutingModule
  ]
})
export class AhorrosModule { }
