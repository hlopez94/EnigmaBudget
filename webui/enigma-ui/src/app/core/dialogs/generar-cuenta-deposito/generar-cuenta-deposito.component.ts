import { CountriesStore } from './../../stores/countries.store';
import { CurrenciesStore } from './../../stores/currencies.store';
import { Observable } from 'rxjs';
import { Divisa } from './../../model/divisa';
import { FormGroup, FormControl, FormBuilder } from '@angular/forms';
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

  $currencies: Observable<Divisa[]>;
  $countries: Observable<Pais[]>;
  countriesLength: number = 5;
  currenciesLength: number = 5;

  constructor(
    fb: FormBuilder,
    private currenciesStore: CurrenciesStore,
    private countriesStore: CountriesStore
  ) {
    this.$currencies = currenciesStore.filteredDivisas;
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
    this.newAccountForm.controls['Currency'].valueChanges.subscribe((change) =>
      this.currenciesStore.filterCurrencies(change)
    );
  }

  getCurrency(code: string): string {
    return getCurrencySymbol(code, 'wide');
  }

  getCountryValue(option: Pais) {
    return option?.name;
  }

  getCurrencyDisplay(option: Divisa): string {
    return option
      ? `${getCurrencySymbol(option.code, 'narrow')} - ${option.name} [${
          option.code
        }]`
      : '';
  }

  async submit(){
    //this.acc
  }
}

export interface CreateDepositAccountForm {
  AccountAlias: FormControl<string>;
  Description: FormControl<string>;
  InitialFunds: FormControl<number>;
  Country: FormControl<Pais>;
  Currency: FormControl<Divisa>;
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
