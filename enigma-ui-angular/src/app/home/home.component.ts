import { CurrenciesStore } from './../core/stores/currencies.store';
import { Component, OnInit } from '@angular/core';
import { Observable } from 'rxjs';
import { Divisa } from '../core/model/divisa';
import { CuentaDeposito } from '../core/model/cuenta-deposito';
import { CuentasDepositoStore } from '../core/stores/cuentas-deposito.store';
@Component({
  selector: 'app-home',
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.scss'],
})
export class HomeComponent implements OnInit {
  cuentasUsuario: Observable<CuentaDeposito[]>;
  divisas: Observable<Divisa[]>;

  constructor(
    private divisasStore: CurrenciesStore,
    private cuentasDepositoStore: CuentasDepositoStore
  ) {
    this.divisas = this.divisasStore.divisas;
    this.cuentasUsuario = this.cuentasDepositoStore.cuentasUser;
  }

  async ngOnInit() {
    await this.cuentasDepositoStore.cargarCuentasUsuario();
  }

  counter(i: number) {
    return new Array(i);
  }

  agregarCuenta() {}
}

