import { TestBed } from '@angular/core/testing';

import { CountriesStore } from './countries.store';

describe('CountriesService', () => {
  let service: CountriesStore;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(CountriesStore);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
