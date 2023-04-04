import { BehaviorSubject, Observable, ReplaySubject } from 'rxjs';
import { Injectable } from '@angular/core';
import { CuentaDeposito } from '../model/cuenta-deposito';
import { CuentasDepositoService } from '../services/cuentas.service';

@Injectable({
  providedIn: 'root'
})
export class CuentasDepositoStore{
private $cuentasUser: ReplaySubject<CuentaDeposito[]>;
cuentasUser : Observable<CuentaDeposito[]>;

  constructor(private cuentasDepositoService: CuentasDepositoService) {
    this.$cuentasUser = new ReplaySubject<CuentaDeposito[]>();
    this.cuentasUser = this.$cuentasUser.asObservable();
  }

  async cargarCuentasUsuario(){
    var res = await this.cuentasDepositoService.cargarCuentasUsuario();
    this.$cuentasUser.next(res);
  }
}
