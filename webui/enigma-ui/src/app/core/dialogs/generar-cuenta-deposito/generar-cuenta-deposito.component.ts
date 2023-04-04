import { CountriesStore } from './../../stores/countries.store';
import { CurrenciesStore } from './../../stores/currencies.store';
import { firstValueFrom, map, Observable, startWith } from 'rxjs';
import { Moneda } from './../../model/moneda';
import {
  FormGroup,
  FormControl,
  FormBuilder,
  Validators,
} from '@angular/forms';
import { Component } from '@angular/core';
import { getCurrencySymbol } from '@angular/common';
import { Pais } from '../../model/pais';

@Component({
  selector: 'app-generar-cuenta-deposito',
  templateUrl: './generar-cuenta-deposito.component.html',
  styleUrls: ['./generar-cuenta-deposito.component.scss'],
})
export class GenerarCuentaDepositoComponent {
  newAccountForm: FormGroup;

  $currencies: Observable<Moneda[]>;
  $countries: Observable<Pais[]>;
  countriesLength: number = 5;
  currenciesLength: number = 5;

  constructor(
    fb: FormBuilder,
    private currenciesStore: CurrenciesStore,
    private countriesStore: CountriesStore
  ) {
    this.$currencies = currenciesStore.divisas;
    this.$countries = countriesStore.filteredCountries;

    this.$countries.subscribe(
      (arr) => (this.countriesLength = arr.length > 5 ? 5 : arr.length)
    );
    this.$currencies.subscribe(
      (arr) => (this.currenciesLength = arr.length > 5 ? 5 : arr.length)
    );

    this.newAccountForm = fb.group<CreateDepositAccountForm>({
      AccountAlias: new FormControl(),
      Description: new FormControl(),
      InitialFunds: new FormControl(),
      Country: new FormControl(),
      Currency: new FormControl(),
      Type: new FormControl(),
    });
  }

  async ngOnInit() {
    await this.currenciesStore.cargarDivisas();
    await this.countriesStore.loadCountries();

    this.newAccountForm.controls['Country'].valueChanges.subscribe((change) =>
      this.countriesStore.filterCountries(change)
    );
  }

  getCurrency(code: string) {
    return getCurrencySymbol(code, 'wide');
  }

  getCountryValue(option: Pais) {
    return option?.name;
  }

  async getCurrenciesAutoCompleteHeight(list: any[]) {
    this.$currencies.pipe(
      map((value) => {
        return value.length * 48;
      })
    );
  }
}

export interface CreateDepositAccountForm {
  AccountAlias: FormControl<string>;
  Description: FormControl<string>;
  InitialFunds: FormControl<number>;
  Country: FormControl<Pais>;
  Currency: FormControl<Moneda>;
  Type: FormControl<DepositAccountType>;
}

export interface BaseType<TEnum> {
  Id: string;
  Name: string;
  TypeEnum: TEnum;
  Description: string;
}

export enum DepositAccountTypesEnum {
  SAVINGS_ACCOUNT,
  CURRENT_ACCOUNT,
  WALLET,
}

export interface DepositAccountType extends BaseType<DepositAccountTypesEnum> {}
