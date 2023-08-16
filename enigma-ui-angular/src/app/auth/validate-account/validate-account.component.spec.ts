import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ValidateAccountComponent } from './validate-account.component';
import { RouterModule } from '@angular/router';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { AuthService } from '../auth.service';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';

describe('ValidateAccountComponent', () => {
  let component: ValidateAccountComponent;
  let fixture: ComponentFixture<ValidateAccountComponent>;

  beforeEach(async () => {
    await 
    TestBed.configureTestingModule({
      declarations:[     
        ],
      imports: [
        RouterModule,
        MatSnackBarModule,
        HttpClientTestingModule,
        ValidateAccountComponent        
      ],
      providers: [
        provideHttpClient(withInterceptorsFromDi()),
        AuthService
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ValidateAccountComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
