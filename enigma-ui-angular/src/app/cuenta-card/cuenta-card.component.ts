import { AppStateService } from 'src/app/core/services/app-state.service';
import { Component, Input } from '@angular/core';
import { CuentaDeposito } from '../core/model/cuenta-deposito';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatButtonModule } from '@angular/material/button';
import { CommonModule } from '@angular/common';
import { MonedaPipe } from '../core/pipes/monedaPipe';
import { Observable } from 'rxjs';

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
    MonedaPipe
  ]
})
export class CuentaCardComponent {
  @Input('type') type!: CuentaCardComponentStatus;
  @Input('account') account: CuentaDeposito | undefined;

  mostrarBalances: Observable<'mostrar'|'ocultar'> = this.appStateService.mostrarBalances;

  constructor(private appStateService:AppStateService){  }
}

export type CuentaCardComponentStatus = 'account' | 'placeholder' | 'loading';
