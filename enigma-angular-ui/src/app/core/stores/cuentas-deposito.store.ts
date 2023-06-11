import { BehaviorSubject, Observable } from 'rxjs';
import { Injectable } from '@angular/core';
import { CuentaDeposito } from '../model/cuenta-deposito';
import { CuentasDepositoService } from '../services/cuentas.service';
import { StoreStateType, StoreStates } from './StoreState';

@Injectable({
  providedIn: 'root',
})
export class CuentasDepositoStore {
  private $cuentasUser: BehaviorSubject<CuentaDeposito[]>;
  cuentasUser: Observable<CuentaDeposito[]>;

  private $storeState: BehaviorSubject<StoreStateType>;
  storeState: Observable<StoreStateType>;

  constructor(private cuentasDepositoService: CuentasDepositoService) {
    this.$cuentasUser = new BehaviorSubject<CuentaDeposito[]>([]);
    this.cuentasUser = this.$cuentasUser.asObservable();

    this.$storeState = new BehaviorSubject<StoreStateType>(StoreStates.SUCCESS);
    this.storeState = this.$storeState.asObservable();
  }

  async cargarCuentasUsuario() {
    this.$storeState.next(StoreStates.LOADING);
    var res = await this.cuentasDepositoService.cargarCuentasUsuario();

    res.subscribe((res) => {
      if(res.isSuccess){
        this.$cuentasUser.next(res.data)
        this.$storeState.next(StoreStates.SUCCESS);
      }else
        this.$storeState.next(StoreStates.FAILURE);
    })
  }
}


