import { CurrenciesService } from '../services/currencies.service';
import { Observable, BehaviorSubject } from 'rxjs';
import { Injectable } from '@angular/core';
import { Balance } from '../model/balance';
import { BalancesService } from '../services/balances.service';

@Injectable({
  providedIn: 'root',
})
export class BalancesStore {
  private $balances: BehaviorSubject<Balance[]>;
  balances: Observable<Balance[]>;


  constructor(private balancesService: BalancesService) {
    this.$balances = new BehaviorSubject<Balance[]>([]);
    this.balances = this.$balances.asObservable();
  }

  async cargarBalances() {
    if (this.$balances.getValue().length == 0) {
      var Balances = await this.balancesService.ObtenerBalances();
      this.$balances.next(Balances);
    }
  }
}
