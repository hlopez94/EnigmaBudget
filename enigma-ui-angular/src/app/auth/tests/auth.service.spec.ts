import { TestBed } from '@angular/core/testing';
import { AuthService } from '../auth.service';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TypedApiResponse } from '../../core/model/ApiResponse';
import { Profile } from '../model/profile';
import { environment } from 'src/environments/environment';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';

describe('AuthService', () => {
  let authService: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        HttpClientTestingModule         
      ],
      providers: [
        provideHttpClient(withInterceptorsFromDi()),
      ]
    });
    httpMock = TestBed.inject(HttpTestingController);
    authService = TestBed.inject(AuthService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(authService).toBeTruthy();
  });

  it('should verify account mail', async () => {
    const token = 'dummyToken';
    const response: TypedApiResponse<boolean> = { isSuccess: true, data: true, errors:[], errorText:'' };
    
    authService.verifyAccountMail(token).then(result => {
      expect(result).toEqual(response);
    });

    const req = httpMock.expectOne(`${environment.settings.apiUrl}/user/verify-email-account`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBe(token);
    req.flush(response);
  });

  it('should get profile', async () => {
    const dummyProfile: Profile = { correo: 'testtest.com', fechaNacimiento:new Date(1994,4,29), nombre:'Test', telefonoCodigoArea: 342, telefonoCodigoNumero: 4069403, telefonoCodigoPais:54 ,usuario: 'userTest'};
    const response: TypedApiResponse<Profile> = { isSuccess: true, data: dummyProfile, errors:[], errorText: ''};

    authService.getProfile().then(result => {
      expect(result).toEqual(dummyProfile);
    });

    const req = httpMock.expectOne(`${environment.settings.apiUrl}/user/profile`);
    expect(req.request.method).toBe('GET');
    req.flush(response);
  });
});
