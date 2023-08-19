import { TestBed } from '@angular/core/testing';
import { AuthService } from '../auth.service';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TypedApiResponse } from '../../core/model/ApiResponse';
import { Profile } from '../model/profile';
import { environment } from 'src/environments/environment';
import { HttpClient, provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';

describe('AuthService', () => {
  let authService: AuthService;
  let httpClient: HttpClient;
  let httpTestingController: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        HttpClientTestingModule         
      ]
    });
    httpClient = TestBed.inject(HttpClient);
    httpTestingController  = TestBed.inject(HttpTestingController);
    authService = TestBed.inject(AuthService);
  });

  afterEach(() => {
    httpTestingController.verify();
  });

  it('should be created', () => {
    expect(authService).toBeTruthy();
  });

  it('should verify account mail', async () => {
    const verifyEmailToken = 'ogfjrewgfj8493q-vajifg043=vjirojq8430';
    const response: TypedApiResponse<boolean> = { isSuccess: true, data: true, errors:[], errorText:'' };
    
    authService.verifyAccountMail(verifyEmailToken).then(result => {
      expect(result).toEqual(response);
    });

    const req = httpTestingController.expectOne(`${environment.settings.apiUrl}/user/verify-email-account`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBe(verifyEmailToken);
    req.flush(response);
  });

  it('should get profile', async () => {
    const dummyProfile: Profile = { correo: 'test@test.com', fechaNacimiento:new Date(1994,4,29), nombre:'Test', telefonoCodigoArea: 342, telefonoCodigoNumero: 4069403, telefonoCodigoPais:54 ,usuario: 'userTest'};
    const response: TypedApiResponse<Profile> = { isSuccess: true, data: dummyProfile, errors:[], errorText: ''};

    authService.getProfile().then(result => {
      expect(result).toEqual(dummyProfile);
    });
    const req = httpTestingController.expectOne(`${environment.settings.apiUrl}/user/profile`);
    expect(req.request.method).toBe('GET');
    req.flush(response);
  });

  it('should update profile', async () => {
    const dummyProfile: Profile = { correo: 'test@test.com', fechaNacimiento:new Date(1994,4,29), nombre:'Test', telefonoCodigoArea: 342, telefonoCodigoNumero: 4069403, telefonoCodigoPais:54 ,usuario: 'userTest'};
    const response: TypedApiResponse<boolean> = { isSuccess: true, data: true, errors:[], errorText: ''};

    authService.updateProfile(dummyProfile).then(result => {
      expect(result).toEqual(true);
    });
    const req = httpTestingController.expectOne(`${environment.settings.apiUrl}/user/profile`);
    expect(req.request.method).toBe('PUT');
    req.flush(response);
  });
});
