import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CuentasComponent } from './cuentas.component';

describe('CuentasComponent', () => {
  let component: CuentasComponent;
  let fixture: ComponentFixture<CuentasComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ CuentasComponent ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(CuentasComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
