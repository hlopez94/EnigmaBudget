import { CountriesService } from '../services/countries.service';
import { Injectable, Signal, computed, signal } from '@angular/core';
import { Pais } from '../model/pais';
import { BaseStore } from './BaseStore';

@Injectable({
  providedIn: 'root',
})
export class PaisesStore extends BaseStore<Pais[]> {

  private _storeFilterValue = signal<string>('');
  public filteredCountries: Signal<Pais[]> = computed(()=> this.$store().status == 'ok' ? this.$store().data!.filter((c) =>  c.name.toLowerCase().includes((this._storeFilterValue() ?? '').toLowerCase())) : [])

  constructor(private countriesService: CountriesService) {
    super();
  }

  async loadCountries() {
    if(this.$store().status == 'ok' && this.$store().data!.length > 0)
      return;

    await this.handleApiRequest(this.countriesService.getAllCountries());    
  }

  filterCountries(value: string):void {
    this._storeFilterValue.set(value ?? '');
  }
}
