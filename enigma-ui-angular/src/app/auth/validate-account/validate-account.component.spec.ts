import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ValidateAccountComponent } from './validate-account.component';

describe('ValidateAccountComponent', () => {
  let component: ValidateAccountComponent;
  let fixture: ComponentFixture<ValidateAccountComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
    imports: [ValidateAccountComponent]
})
    .compileComponents();

    fixture = TestBed.createComponent(ValidateAccountComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
