import { Observable, firstValueFrom } from 'rxjs';
import { environment } from 'src/environments/environment';
import { ApiResponse, TypedApiResponse } from 'src/app/core/model/ApiResponse';
import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { CuentaDeposito } from 'src/app/core/model/cuenta-deposito';
import { TipoCuentaDeposito } from '../model/TipoCuentaDeposito';
import { Pais } from '../model/pais';
import { Divisa } from '../model/divisa';

@Injectable({
  providedIn: 'root',
})
export class CuentasDepositoService {
  constructor(private httpClient: HttpClient) {}

  cargarCuentasUsuario(): Observable<TypedApiResponse<CuentaDeposito[]>> {
    return this.httpClient.get<TypedApiResponse<CuentaDeposito[]>>(
      `${environment.settings.apiUrl}/DepositAccounts`
    );
  }
  cargarTiposCuentasDeposito(): Observable<TypedApiResponse<TipoCuentaDeposito[]>> {
    return this.httpClient.get<TypedApiResponse<TipoCuentaDeposito[]>>(
      `${environment.settings.apiUrl}/DepositAccountTypes`
    );
  }

  crearCuentaDeposito(nuevaCuenta: CrearCuentaDepositoRequest): Observable<TypedApiResponse<CuentaDeposito>>{
    return this.httpClient.post<TypedApiResponse<CuentaDeposito>>(
      `${environment.settings.apiUrl}/DepositAccounts`, nuevaCuenta
    );
  }

  modificarCuentaDeposito(idCuenta: number,cuentaDeposito:CuentaDeposito): Observable<TypedApiResponse<CuentaDeposito>>{
    return this.httpClient.put<TypedApiResponse<CuentaDeposito>>(
      `${environment.settings.apiUrl}/DepositAccounts/${idCuenta}`, cuentaDeposito
    );
  }

  eliminarCuentaDeposito(idCuenta: number): Observable<ApiResponse>{
    return this.httpClient.delete<TypedApiResponse<CuentaDeposito>>(
      `${environment.settings.apiUrl}/DepositAccounts/${idCuenta}`
    );
  }
}

export interface CrearCuentaDepositoRequest{
  AccountAlias : string ;
  Description : string ;
  InitialFunds : number ;
  Country : Pais ;
  Currency : Divisa ;
  Type : TipoCuentaDeposito ;
}
