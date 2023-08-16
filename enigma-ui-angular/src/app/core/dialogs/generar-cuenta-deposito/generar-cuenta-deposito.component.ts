import { PaisesStore } from '../../stores/paises.store';
import { DivisasStore } from '../../stores/divisas.store';
import { Observable } from 'rxjs';
import { Divisa } from './../../model/divisa';
import {
  FormGroup,
  FormControl,
  FormBuilder,
  ReactiveFormsModule,
} from '@angular/forms';
import { Component, EventEmitter, Output, Signal, computed } from '@angular/core';
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
import { CreateDepositAccountForm } from './CreateDepositAccountForm';

@Component({
  selector: 'app-generar-cuenta-deposito',
  templateUrl: './generar-cuenta-deposito.component.html',
  styleUrls: ['./generar-cuenta-deposito.component.scss'], 
  standalone: true,
  imports: [
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
    MatButtonToggleModule,
  ],
})
export class GenerarCuentaDepositoComponent {
  newAccountForm: FormGroup;

  @Output('resultado') resultadoEvent: EventEmitter<'creado' | 'error'> = new EventEmitter();

  readonly $currencies: Signal<Divisa[]> = this.currenciesStore.filteredDivisas;
  readonly $countries: Signal<Pais[]> = this.countriesStore.filteredCountries;
  readonly $tiposCuenta: Observable<TipoCuentaDeposito[]> = this.cuentasStore.tiposCuentaDeposito;

  //countriesLength: number = 5;
  currenciesLength: Signal<number> = computed(()=> {return this.$currencies() ? this.$currencies()!.length < 5 ? this.$currencies()!.length : 5: 5;});
  countriesLength: Signal<number> = computed(()=> {return this.$countries() ? this.$countries()!.length < 5 ? this.$countries()!.length : 5: 5;});

  constructor(
    fb: FormBuilder,
    private currenciesStore: DivisasStore,
    private countriesStore: PaisesStore,
    public cuentasStore: CuentasDepositoStore
  ) {
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
    await this.cuentasStore.cargarTiposCuentaDeposito();

    this.newAccountForm.controls['Country'].valueChanges.subscribe((change) =>{
      if(typeof(change) == 'string')
        this.countriesStore.filterCountries(change)
    }
    );
    this.newAccountForm.controls['Currency'].valueChanges.subscribe((change) =>{
      if(typeof(change) == 'string')
        this.currenciesStore.filterCurrencies(change)
      }
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

  async submit() {
    await this.cuentasStore.crearCuentaDeposito(this.newAccountForm.value);
    this.resultadoEvent.emit('creado');
  }
}


