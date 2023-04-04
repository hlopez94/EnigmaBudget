import { firstValueFrom } from 'rxjs';
import { environment } from 'src/environments/environment';
import { ApiResponse } from 'src/app/core/model/ApiResponse';
import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { CuentaDeposito } from 'src/app/core/model/cuenta-deposito';

@Injectable({
  providedIn: 'root'
})
export class CuentasDepositoService {

  constructor(private httpClient: HttpClient) { }

  async cargarCuentasUsuario() : Promise<CuentaDeposito[]>{
    var res = await firstValueFrom(
      this.httpClient.get<ApiResponse<CuentaDeposito[]>>(
        `${environment.settings.apiUrl}/DepositAccounts`
      )
    );
    return res.data;
  }
}
