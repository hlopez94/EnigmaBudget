import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { canActivateAuth } from './auth/auth-guard';

const routes: Routes = [
  {
    path:'',
    loadComponent:() => import('./shell/welcome-page/welcome-page.component').then(c=>c.WelcomePageComponent)
  },
  {
    path:'user-dashboard',
    loadChildren: () => import('./home/home.module').then(m => m.HomeModule),
    canActivate:[canActivateAuth]
  },
  {
    path:'tarjetas',
    loadChildren: () => import('./tarjetas/tarjetas.module').then(m => m.TarjetasModule),
    canActivate:[canActivateAuth]
  },
  {
    path:'cuentas',
    loadChildren: () => import('./cuentas/cuentas.module').then(m => m.CuentasModule),
    canActivate:[canActivateAuth]
  },
  {
    path:'reportes',
    loadChildren: () => import('./reportes/reportes.module').then(m => m.ReportesModule),
    canActivate:[canActivateAuth]
  },
  {
    path:'tarjetas',
    loadChildren: () => import('./tarjetas/tarjetas.module').then(m => m.TarjetasModule),
    canActivate:[canActivateAuth]
  },
  {
    path:'auth',
    loadChildren: () => import('./auth/auth.module').then(m => m.AuthModule)
  },
  {
    path: '*',
    redirectTo: ''
  }
];

@NgModule({
  imports: [RouterModule.forRoot(routes, {useHash:true})],
  exports: [RouterModule]
})
export class AppRoutingModule { }
