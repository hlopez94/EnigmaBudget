import { CountriesStore } from './../../stores/countries.store';
import { CurrenciesStore } from './../../stores/currencies.store';
import { Observable } from 'rxjs';
import { Divisa } from './../../model/divisa';
import { FormGroup, FormControl, FormBuilder, Validators, ReactiveFormsModule } from '@angular/forms';
import { Component } from '@angular/core';
import { CommonModule, getCurrencySymbol } from '@angular/common';
import { Pais } from '../../model/pais';
import { TipoCuentaDeposito } from '../../model/TipoCuentaDeposito';
import { CuentasDepositoStore } from '../../stores/cuentas-deposito.store';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

@Component({
  selector: 'app-generar-cuenta-deposito',
  templateUrl: './generar-cuenta-deposito.component.html',
  styleUrls: ['./generar-cuenta-deposito.component.scss'],
  standalone:true,
  imports:[
    CommonModule,
    MatChipsModule,
    MatAutocompleteModule,
    MatDialogModule,
    MatButtonModule,
    ReactiveFormsModule,
    MatInputModule,
    MatSelectModule,
    MatFormFieldModule,
    MatInputModule,
    ScrollingModule,
    MatIconModule,
    MatButtonToggleModule
  ],
  providers:[

  ]
})
export class GenerarCuentaDepositoComponent {
  newAccountForm: FormGroup;

  readonly $currencies: Observable<Divisa[]> = this.currenciesStore.filteredDivisas;
  readonly $countries: Observable<Pais[]> = this.countriesStore.filteredCountries;
  readonly $tiposCuenta: Observable<TipoCuentaDeposito[]> = this.cuentasStore.tiposCuentaDeposito;

  countriesLength: number = 5;
  currenciesLength: number = 5;

  constructor(
    fb: FormBuilder,
    private currenciesStore: CurrenciesStore,
    private countriesStore: CountriesStore,
    private cuentasStore: CuentasDepositoStore
  ) {

    this.newAccountForm = fb.group<CreateDepositAccountForm>({
      AccountAlias: new FormControl(),
      Description: new FormControl(),
      InitialFunds: new FormControl(),
      Country: new FormControl(),
      Currency: new FormControl(),
      Type: new FormControl(),
    });

    this.$countries.subscribe(
      (arr) => (this.countriesLength = arr.length > 5 ? 5 : arr.length)
    );
    this.$currencies.subscribe(
      (arr) => (this.currenciesLength = arr.length > 5 ? 5 : arr.length)
    );

  }

  async ngOnInit() {
    await this.currenciesStore.cargarDivisas();
    await this.countriesStore.loadCountries();
    await this.cuentasStore.cargarTiposCuentaDeposito();

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
    await this.cuentasStore.crearCuentaDeposito(this.newAccountForm.value)
  }
}

export interface CreateDepositAccountForm {
  AccountAlias: FormControl<string>;
  Description: FormControl<string>;
  InitialFunds: FormControl<number>;
  Country: FormControl<Pais>;
  Currency: FormControl<Divisa>;
  Type: FormControl<TipoCuentaDeposito>;
}
