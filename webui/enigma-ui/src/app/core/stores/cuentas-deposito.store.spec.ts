import { TestBed } from '@angular/core/testing';

import { CuentasDepositoStore } from './cuentas-deposito.store';

describe('CuentasDepositoService', () => {
  let service: CuentasDepositoStore;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(CuentasDepositoStore);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
