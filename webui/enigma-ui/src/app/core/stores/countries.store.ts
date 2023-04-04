import { CountriesService } from './../services/countries.service';
import { BehaviorSubject, Observable, ObservedValueOf } from 'rxjs';
import { Injectable } from '@angular/core';
import { Pais } from '../model/pais';

@Injectable({
  providedIn: 'root',
})
export class CountriesStore {
  private $countries: BehaviorSubject<Pais[]>;
  public countries: Observable<Pais[]>;
  private $filteredCountries: BehaviorSubject<Pais[]>;
  public filteredCountries: Observable<Pais[]>;

  constructor(private countriesService: CountriesService) {
    this.$countries = new BehaviorSubject<Pais[]>([]);
    this.countries = this.$countries.asObservable();
    this.$filteredCountries= new BehaviorSubject<Pais[]>([]);
    this.filteredCountries= this.$filteredCountries.asObservable();

  }

  async loadCountries() {
    if (this.$countries.value.length == 0) {
      var paises = await this.countriesService.getAllCountries();
      this.$countries.next(paises);
      this.$filteredCountries.next(paises);
    }
  }

  filterCountries(value: string):void {
    var filtered= this.$countries.value.filter((c) =>
      c.name.toLowerCase().includes(value.toLowerCase())
    );
    this.$filteredCountries.next(filtered);
  }
}
