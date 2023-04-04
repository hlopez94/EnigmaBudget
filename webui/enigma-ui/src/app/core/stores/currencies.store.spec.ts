import { TestBed } from '@angular/core/testing';

import { CurrenciesStore } from './currencies.store';

describe('CurrenciesStore', () => {
  let service: CurrenciesStore;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(CurrenciesStore);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
