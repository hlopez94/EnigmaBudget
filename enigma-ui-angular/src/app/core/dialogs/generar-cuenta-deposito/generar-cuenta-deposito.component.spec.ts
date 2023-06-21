import { ComponentFixture, TestBed } from '@angular/core/testing';

import { GenerarCuentaDepositoComponent } from './generar-cuenta-deposito.component';

describe('GenerarCuentaDepositoComponent', () => {
  let component: GenerarCuentaDepositoComponent;
  let fixture: ComponentFixture<GenerarCuentaDepositoComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
    imports: [GenerarCuentaDepositoComponent]
})
    .compileComponents();

    fixture = TestBed.createComponent(GenerarCuentaDepositoComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
