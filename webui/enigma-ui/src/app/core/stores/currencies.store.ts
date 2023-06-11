import { CurrenciesService } from './../services/currencies.service';
import { Divisa } from './../model/divisa';
import { Observable, BehaviorSubject } from 'rxjs';
import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root',
})
export class CurrenciesStore {
  private $divisas: BehaviorSubject<Divisa[]>;
  divisas: Observable<Divisa[]>;

  private $filteredDivisas: BehaviorSubject<Divisa[]>;
  public filteredDivisas: Observable<Divisa[]>;

  constructor(private currenciesService: CurrenciesService) {
    this.$divisas = new BehaviorSubject<Divisa[]>([]);
    this.divisas = this.$divisas.asObservable();

    this.$filteredDivisas = new BehaviorSubject<Divisa[]>([]);
    this.filteredDivisas = this.$filteredDivisas.asObservable();
  }

  async cargarDivisas() {
    if (this.$divisas.getValue().length == 0) {
      var divisas = await this.currenciesService.ObtenerTodas();
      this.$divisas.next(divisas);
    }
  }
  filterCurrencies(value: string): void {
    var filtered = this.$divisas.value.filter((c) =>
      c.name.toLowerCase().includes(value.toLowerCase())
    );
    this.$filteredDivisas.next(filtered);
  }
}
