import { BehaviorSubject, Observable } from 'rxjs';
import { Injectable } from '@angular/core';
import { CuentaDeposito } from '../model/cuenta-deposito';
import { CrearCuentaDepositoRequest, CuentasDepositoService } from '../services/cuentas.service';
import { StoreStateType, StoreStates } from './StoreState';
import { TipoCuentaDeposito } from '../model/TipoCuentaDeposito';

@Injectable({
  providedIn: 'root',
})
export class CuentasDepositoStore {
  private $cuentasUser: BehaviorSubject<CuentaDeposito[] | null>;
  cuentasUser: Observable<CuentaDeposito[] | null>;

  private $storeState: BehaviorSubject<StoreStateType>;
  storeState: Observable<StoreStateType>;

  private $storeTipoState: BehaviorSubject<StoreStateType>;
  storeTipoState:Observable<StoreStateType>;

  private $tiposCuentaDeposito: BehaviorSubject<TipoCuentaDeposito[]>;
  tiposCuentaDeposito : Observable<TipoCuentaDeposito[]>;

  constructor(private cuentasDepositoService: CuentasDepositoService) {
    this.$cuentasUser = new BehaviorSubject<CuentaDeposito[] | null>(null);
    this.cuentasUser = this.$cuentasUser.asObservable();

    this.$storeState = new BehaviorSubject<StoreStateType>(StoreStates.SUCCESS);
    this.storeState = this.$storeState.asObservable();

    this.$tiposCuentaDeposito = new BehaviorSubject<TipoCuentaDeposito[]>([]);
    this.tiposCuentaDeposito = this.$tiposCuentaDeposito.asObservable();

    this.$storeTipoState = new BehaviorSubject<StoreStateType>(StoreStates.SUCCESS);
    this.storeTipoState = this.$storeTipoState.asObservable();
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
  async cargarTiposCuentaDeposito() {
    this.$storeTipoState.next(StoreStates.LOADING);
    var res = await this.cuentasDepositoService.cargarTiposCuentasDeposito();

    res.subscribe((res) => {
      if(res.isSuccess){
        this.$tiposCuentaDeposito.next(res.data)
        this.$storeTipoState.next(StoreStates.SUCCESS);
      }else
        this.$storeTipoState.next(StoreStates.FAILURE);
    })
  }

  async crearCuentaDeposito(nuevaCuenta: CrearCuentaDepositoRequest){
    var res = await this.cuentasDepositoService.crearCuentaDeposito( nuevaCuenta);

    res.subscribe(async (res)=>{
      if(res.isSuccess){
        await this.cargarCuentasUsuario();
      }else
      this.$storeState.next(StoreStates.FAILURE);
    })
  }
}


