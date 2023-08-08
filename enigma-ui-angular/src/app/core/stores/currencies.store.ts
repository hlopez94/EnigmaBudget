import { CurrenciesService } from './../services/currencies.service';
import { Divisa } from './../model/divisa';
import { Injectable, Signal, computed, signal } from '@angular/core';
import { BaseStore } from './BaseStore';

@Injectable({
  providedIn: 'root',
})
export class CurrenciesStore extends BaseStore<Divisa[]> {
 
  private _filterValue = signal<string>('');
  public filteredDivisas : Signal<Divisa[]> =  computed(()=> this.$store().data ? this.$store().data!.filter((c) =>  c.name.toLowerCase().includes(this._filterValue().toLowerCase())) : [])


  constructor(private currenciesService: CurrenciesService) {
    super();
  }

  async cargarDivisas() {
    if (this.$store().data?.length == 0) {
      var divisas = this.currenciesService.ObtenerTodas();
      this.handleApiRequest(divisas);
    }
  }
  
  filterCurrencies(value: string): void {
    this._filterValue.set(value);
  }
}
