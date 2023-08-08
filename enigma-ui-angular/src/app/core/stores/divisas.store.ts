import { CurrenciesService } from '../services/currencies.service';
import { Divisa } from '../model/divisa';
import { Injectable, Signal, computed, signal } from '@angular/core';
import { BaseStore } from './BaseStore';

@Injectable({
  providedIn: 'root',
})
export class DivisasStore extends BaseStore<Divisa[]> {
 
  private _storeFilterValue = signal<string>('');
  public filteredDivisas : Signal<Divisa[]> =  computed(()=> this.$store().status == 'ok' ? this.$store().data!.filter((c) =>  c.name.toLowerCase().includes((this._storeFilterValue() ?? '').toLowerCase())) : [])


  constructor(private currenciesService: CurrenciesService) {
    super();
  }

  async cargarDivisas() {
    if(this.$store().status == 'ok' && this.$store().data!.length > 0)
      return;

    await this.handleApiRequest(this.currenciesService.ObtenerTodas());    
  }
  
  filterCurrencies(value: string) {
    this._storeFilterValue.set(value ?? '');
  }
}
