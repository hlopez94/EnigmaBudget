import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { AuthGuard } from './auth/auth-guard';
import { HomeComponent } from './home/home.component';

const routes: Routes = [
  {
    path:'',
    component:HomeComponent
  },
  {
    path:'test',
    component:HomeComponent,
    canActivate:[AuthGuard]
  }
];

@NgModule({
  imports: [RouterModule.forRoot(routes, {useHash:true})],
  exports: [RouterModule]
})
export class AppRoutingModule { }
