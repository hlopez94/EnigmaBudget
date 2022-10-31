import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

import { TarjetasRoutingModule } from './tarjetas-routing.module';
import { TarjetasComponent } from './tarjetas.component';


@NgModule({
  declarations: [
    TarjetasComponent
  ],
  imports: [
    CommonModule,
    TarjetasRoutingModule
  ]
})
export class TarjetasModule { }
