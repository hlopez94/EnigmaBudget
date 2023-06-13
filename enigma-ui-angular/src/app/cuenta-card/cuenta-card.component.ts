import { Component, Input } from '@angular/core';
import { CuentaDeposito } from '../core/model/cuenta-deposito';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatButtonModule } from '@angular/material/button';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-cuenta-card',
  templateUrl: './cuenta-card.component.html',
  styleUrls: ['./cuenta-card.component.scss'],
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatIconModule,
    MatMenuModule,
    MatButtonModule,
  ]
})
export class CuentaCardComponent {
  @Input('type') type!: CuentaCardComponentStatus;
  @Input('account') account: CuentaDeposito | undefined;
}

export type CuentaCardComponentStatus = 'account' | 'placeholder' | 'loading';
