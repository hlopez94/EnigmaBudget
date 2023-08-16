import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GenerarCuentaDepositoComponent } from './generar-cuenta-deposito.component';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { DivisasStore } from '../../stores/divisas.store';
import { PaisesStore } from '../../stores/paises.store';
import { CuentasDepositoStore } from '../../stores/cuentas-deposito.store';
import { MatInputModule } from '@angular/material/input';
import {  MatSelectModule } from '@angular/material/select';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { Pais } from '../../model/pais';
import { TipoCuentaDeposito } from '../../model/TipoCuentaDeposito';
import { TipoCuentaDepositoEnum } from '../../model/TipoCuentaDepositoEnum';
import { Divisa } from '../../model/divisa';
import {  provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';

describe('GenerarCuentaDepositoComponent', () => {
  let component: GenerarCuentaDepositoComponent;
  let fixture: ComponentFixture<GenerarCuentaDepositoComponent>;

  const testCountry : Pais = {alpha2:'AR', alpha3: 'ARG', name: 'Argentina', numericCode:54};
  const testCurr: Divisa = {name: 'Peso Argentino', country : testCountry, code: '$', exponent: 2, num: 1};
  const testType: TipoCuentaDeposito= {description:'Test', iconString: 'test', id	:'1',name:'test', typeEnum: TipoCuentaDepositoEnum.BILLETERA_FISICA};
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        ReactiveFormsModule,
        MatInputModule,
        MatSelectModule,
        BrowserAnimationsModule ,
        GenerarCuentaDepositoComponent
      ],
      providers: [
        provideHttpClient(withInterceptorsFromDi()),
        FormBuilder,
        DivisasStore,        
        PaisesStore,
        CuentasDepositoStore,
      ],
    }).compileComponents();
  });

  beforeEach(() => {
    fixture = TestBed.createComponent(GenerarCuentaDepositoComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should initialize newAccountForm', () => {
    expect(component.newAccountForm).toBeDefined();
  });

  it('should return country name from getCountryValue', () => {
    const country: any = { name: 'TestCountry' };
    const result = component.getCountryValue(country);
    expect(result).toEqual('TestCountry');
  });
  
  it('should return currency display from getCurrencyDisplay', () => {
    const currency: any = { code: 'USD', name: 'US Dollar' };
    const result = component.getCurrencyDisplay(currency);
    expect(result).toEqual('$ - US Dollar [USD]');
  });

  it('should call crearCuentaDeposito and emit resultadoEvent on submit', async () => {
    spyOn(component.cuentasStore, 'crearCuentaDeposito').and.stub();
    spyOn(component.resultadoEvent, 'emit').and.stub();
  
    component.newAccountForm.setValue({
      AccountAlias: 'TestAlias',
      Description: 'Test Description',
      InitialFunds: 100,
      Country: 'TestCountry',
      Currency: 'USD',
      Type: 'TestType',
    });
  
    await component.submit();
      expect(component.cuentasStore.crearCuentaDeposito).toHaveBeenCalledWith({
      AccountAlias: 'TestAlias',
      Description: 'Test Description',
      InitialFunds: 100,
      Country: testCountry,
      Currency: testCurr,
      Type: testType,
    });
  
    expect(component.resultadoEvent.emit).toHaveBeenCalledWith('creado');
  });
  it('should call crearCuentaDeposito and emit resultadoEvent on submit', async () => {
    spyOn(component.cuentasStore, 'crearCuentaDeposito').and.stub();
    spyOn(component.resultadoEvent, 'emit').and.stub();
  
    component.newAccountForm.setValue({
      AccountAlias: 'TestAlias',
      Description: 'Test Description',
      InitialFunds: 100,
      Country: testCountry,
      Currency: testCurr,
      Type: testType
    });
  
    await component.submit();
  
    expect(component.cuentasStore.crearCuentaDeposito).toHaveBeenCalledWith({
      AccountAlias: 'TestAlias',
      Description: 'Test Description',
      InitialFunds: 100,
      Country: testCountry,
      Currency: testCurr,
      Type: testType
    });
  
    expect(component.resultadoEvent.emit).toHaveBeenCalledWith('creado');
  });
});
