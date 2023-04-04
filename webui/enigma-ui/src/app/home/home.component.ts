import { CurrenciesStore } from './../core/stores/currencies.store';
import { Component, OnInit } from '@angular/core';
import { Observable } from 'rxjs';
import { CuentaDeposito } from '../core/model/cuenta-deposito';
import { CuentasDepositoStore } from '../core/stores/cuentas-deposito.store';
import { Moneda } from '../core/model/moneda';

@Component({
  selector: 'app-home',
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.scss'],
})
export class HomeComponent implements OnInit {
  cuentasUsuario: Observable<CuentaDeposito[]>;
  divisas : Observable<Moneda[]>;
  constructor(private cuentasStore: CuentasDepositoStore,
    private divisasStore: CurrenciesStore) {
    this.cuentasUsuario = this.cuentasStore.cuentasUser
    this.divisas=this.divisasStore.divisas;
  }

  async ngOnInit() {
    await this.cuentasStore.cargarCuentasUsuario();
  }

  counter(i: number) {
    return new Array(i);
  }

  agregarCuenta(){
  }
}
