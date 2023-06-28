import { environment } from 'src/environments/environment';
import { TypedApiResponse } from 'src/app/core/model/ApiResponse';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Injectable } from '@angular/core';
import { Balance } from '../model/balance';

@Injectable({
  providedIn: 'root',
})
export class BalancesService {
  constructor(private httpClient: HttpClient) {}

  async ObtenerBalances(): Promise<Balance[]> {
    var res = await firstValueFrom(
      this.httpClient.get<TypedApiResponse<Balance[]>>(
        `${environment.settings.apiUrl}/balances`
      )
    );
    return res.data;
  }
}
