import { Observable, firstValueFrom } from 'rxjs';
import { environment } from 'src/environments/environment';
import { ApiResponse, TypedApiResponse } from 'src/app/core/model/ApiResponse';
import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { CuentaDeposito } from 'src/app/core/model/cuenta-deposito';

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

  crearCuentaDeposito(nuevaCuenta:CuentaDeposito): Observable<TypedApiResponse<CuentaDeposito>>{
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
