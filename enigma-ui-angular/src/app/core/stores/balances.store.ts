import { Injectable } from '@angular/core';
import { Balance } from '../model/balance';
import { BalancesService } from '../services/balances.service';
import { BaseStore } from "./BaseStore";

@Injectable({
  providedIn: 'root',
})
export class BalancesStore extends BaseStore<Balance[]>{

  constructor(private balancesService: BalancesService) {
    super();
  }

  async cargarBalances() {
    await this.handleApiRequest(this.balancesService.obtenerBalances());
  }
}