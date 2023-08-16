import { DivisasStore } from '../core/stores/divisas.store';
import { Component, OnInit, Signal } from '@angular/core';
import { Observable } from 'rxjs';
import { Divisa } from '../core/model/divisa';
import { CuentaDeposito } from '../core/model/cuenta-deposito';
import { CuentasDepositoStore } from '../core/stores/cuentas-deposito.store';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { GenerarCuentaDepositoDialog } from '../core/dialogs/generar-cuenta-deposito/GenerarCuentaDepositoDialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { NgFor, NgIf, CurrencyPipe, AsyncPipe, UpperCasePipe } from '@angular/common';
import { CuentaCardComponent } from '../cuenta-card/cuenta-card.component';
import { Balance } from '../core/model/balance';
import { BalancesStore } from '../core/stores/balances.store';
import { MonedaPipe } from '../core/pipes/monedaPipe';
import { Store } from "../core/stores/Store";
@Component({
    selector: 'app-home',
    templateUrl: './home.component.html',
    styleUrls: ['./home.component.scss'],
    standalone: true,
    imports: [
        NgFor,
        MatCardModule,
        MatButtonModule,
        MatIconModule,
        MatMenuModule,
        MatListModule,
        MatDialogModule,
        NgIf,
        MatDividerModule,
        AsyncPipe,
        CurrencyPipe,
        CuentaCardComponent,
        UpperCasePipe,
        MonedaPipe
    ],
})
export class HomeComponent implements OnInit {
  $cuentasUsuario: Observable<CuentaDeposito[] | null>;
  $balancesUsuario : Signal<Store<Balance[]>>;
  divisas: Signal<Store<Divisa[]>>;

  constructor(
    private divisasStore: DivisasStore,
    private cuentasDepositoStore: CuentasDepositoStore,
    private balancesStore: BalancesStore,
    private dialog: MatDialog
  ) {
    this.divisas = this.divisasStore._store;
    this.$cuentasUsuario = this.cuentasDepositoStore.cuentasUser;
    this.$balancesUsuario = this.balancesStore._store;
  }

  $tiposCuentaDeposito = this.cuentasDepositoStore.tiposCuentaDeposito;

  async ngOnInit() {
    await this.cuentasDepositoStore.cargarCuentasUsuario();
    await this.balancesStore.cargarBalances();
  }

  counter(i: number) {
    return new Array(i);
  }

  agregarCuenta() {
    const dialogRef = this.dialog.open(GenerarCuentaDepositoDialog, {
    });

    dialogRef.afterClosed().subscribe(result => {
    });
  }
}

