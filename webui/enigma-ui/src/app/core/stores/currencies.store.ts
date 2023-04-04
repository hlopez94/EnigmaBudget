import { CurrenciesService } from './../services/currencies.service';
import { Moneda } from './../model/moneda';
import { ReplaySubject, Observable, BehaviorSubject } from 'rxjs';
import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root',
})
export class CurrenciesStore {
  private $divisas: BehaviorSubject<Moneda[]>;
  divisas: Observable<Moneda[]>;

  constructor(private currenciesService: CurrenciesService) {
    this.$divisas = new BehaviorSubject<Moneda[]>([]);
    this.divisas = this.$divisas.asObservable();
  }

  async cargarDivisas() {
    if(this.$divisas.getValue().length == 0){
      var divisas = await this.currenciesService.ObtenerTodas();
      this.$divisas.next(divisas);

    }
  }
}
