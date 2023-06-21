import { CurrenciesStore } from './../core/stores/currencies.store';
import { Component, OnInit } from '@angular/core';
import { Observable } from 'rxjs';
import { Divisa } from '../core/model/divisa';
import { CuentaDeposito } from '../core/model/cuenta-deposito';
import { CuentasDepositoStore } from '../core/stores/cuentas-deposito.store';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { GenerarCuentaDepositoDialog } from '../core/dialogs/generar-cuenta-deposito/generar-cuenta-deposito.component';
import { MatDividerModule } from '@angular/material/divider';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { NgFor, NgIf, CurrencyPipe } from '@angular/common';
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
        CurrencyPipe,
    ],
})
export class HomeComponent implements OnInit {
  cuentasUsuario: Observable<CuentaDeposito[]>;
  divisas: Observable<Divisa[]>;

  constructor(
    private divisasStore: CurrenciesStore,
    private cuentasDepositoStore: CuentasDepositoStore,
    private dialog: MatDialog
  ) {
    this.divisas = this.divisasStore.divisas;
    this.cuentasUsuario = this.cuentasDepositoStore.cuentasUser;
  }

  $tiposCuentaDeposito = this.cuentasDepositoStore.tiposCuentaDeposito;

  async ngOnInit() {
    await this.cuentasDepositoStore.cargarCuentasUsuario();
  }

  counter(i: number) {
    return new Array(i);
  }

  agregarCuenta() {
    const dialogRef = this.dialog.open(GenerarCuentaDepositoDialog, {
     // data: {name: this.name, animal: this.animal},
    });

    dialogRef.afterClosed().subscribe(result => {
      console.log('The dialog was closed');
     // this.animal = result;
    });
  }
}

