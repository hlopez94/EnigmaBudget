import { Component, Input } from '@angular/core';
import { CuentaDeposito } from '../core/model/cuenta-deposito';

@Component({
  selector: 'app-cuenta-card',
  templateUrl: './cuenta-card.component.html',
  styleUrls: ['./cuenta-card.component.scss']
})
export class CuentaCardComponent {
  @Input('type') type!: 'account' | 'placeholder' | 'loading';
  @Input('account') account: CuentaDeposito | undefined;

}
