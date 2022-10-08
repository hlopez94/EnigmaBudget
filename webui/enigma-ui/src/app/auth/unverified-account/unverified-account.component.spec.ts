import { ComponentFixture, TestBed } from '@angular/core/testing';

import { UnverifiedAccountComponent } from './unverified-account.component';

describe('UnverifiedAccountComponent', () => {
  let component: UnverifiedAccountComponent;
  let fixture: ComponentFixture<UnverifiedAccountComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ UnverifiedAccountComponent ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(UnverifiedAccountComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
